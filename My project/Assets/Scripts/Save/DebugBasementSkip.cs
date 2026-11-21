#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using FS_Atmo;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// Dev shortcut: Ctrl+1 (top-row or numpad) teleports the player just outside the basement exit door and
/// applies the same leave-basement state as walking out (butcher may leave the basement again after grace,
/// cage occupancy cleared, leave-basement car shot / checkpoint). Editor and development builds only.
/// Ctrl+2 does the same, then once the shot hands back control drops the player in the farmhouse living room
/// on the Captive layer with the hunt called off, so the butcher roams freely and ignores them (listening test).
/// While listening the Captive layer is held and every player AudioSource (footsteps, breathing) is muted.
/// Ctrl+1 afterwards ends listen mode in place (Player layer + unmute) without teleporting again.
/// Ctrl+3 (top-row or numpad) skips to the cage room after the return scare and wall reveal, with the bar still
/// behind him ready to pick up (same beat as walking back before the auto-save).
/// </summary>
public class DebugBasementSkip : MonoBehaviour
{
    const string ExitDoorName = "Door Basement Exit";
    const float OutsideClearance = 1.25f;
    const string FarmhousePath = "/_Environment/Farmhouse";
    static readonly string[] HouseSpawnAnchors = { "Probe Living Room", "Hotspot_Living_Fireplace" };
    const float LeaveShotTimeout = 45f;
    const string LeaveShotLock = "butcher_window_shot";

    static DebugBasementSkip _instance;
    bool _busy;
    bool _skipped;
    bool _leaveShotStarted;
    GameObject _listenPlayer;
    readonly List<AudioSource> _mutedSources = new List<AudioSource>();
    readonly List<AudioSource> _sourceScratch = new List<AudioSource>();

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
        if (kb == null || !kb.ctrlKey.isPressed)
            return;

        bool press1 = kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame;
        bool press2 = kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame;
        bool press3 = kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame;
        if (!press1 && !press2 && !press3)
            return;

        // Ctrl+1 while listening: become visible again in place (do not re-run the outdoor skip).
        if (press1 && _listenPlayer != null)
        {
            ExitListenModeVisible();
            return;
        }

        // A new shortcut cancels an in-flight skip (Ctrl+2 used to hold _busy through the whole leave shot).
        if (_busy)
            StopAllCoroutines();
        _busy = false;

        if (press1)
            StartCoroutine(Run(listenInside: false));
        else if (press2)
            StartCoroutine(Run(listenInside: true));
        else
            StartCoroutine(RunBarPickup());
    }

    void LateUpdate()
    {
        if (_listenPlayer == null)
        {
            if (_mutedSources.Count > 0)
                StopListening();
            return;
        }

        // Hiding spots and cages rewrite the layer on exit; HiddenPlayer is just as invisible, so leave that alone.
        int captiveLayer = CaptiveLayer();
        int layer = _listenPlayer.layer;
        if (layer != captiveLayer && layer != LayerMask.NameToLayer("HiddenPlayer"))
            _listenPlayer.layer = captiveLayer;

        MutePlayerAudio();
    }

    IEnumerator Run(bool listenInside)
    {
        StopListening();
        _busy = true;
        try
        {
            yield return SkipOutOfBasement();
            if (listenInside && _skipped)
                yield return ListenFromInsideHouse();
        }
        finally
        {
            _busy = false;
        }
    }

    IEnumerator RunBarPickup()
    {
        StopListening();
        _busy = true;
        try
        {
            yield return SkipToBarPickup();
        }
        finally
        {
            _busy = false;
        }
    }

    IEnumerator SkipOutOfBasement()
    {
        _skipped = false;
        _leaveShotStarted = false;

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

        // Same as Continue: strip Captive/HiddenPlayer before teleport so NPCs can see/hear you.
        // Rebind alone used to leave Captive on when you land outside the cage volume.
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            player.layer = playerLayer;

        var hider = player.GetComponent<PlayerHider>();
        if (hider != null)
        {
            if (hider.isHidden && hider.CurrentHidingSpot != null)
                hider.CurrentHidingSpot.RevealPlayer(player);
            hider.ClearSearchMemory();
            hider.EnableControls();
            hider.RestoreCollision();
        }

        Teleport(player, feet, yaw);

        // Drop stale cage hold; outside the volume this also forces Player layer again.
        Cage.RebindAllAfterContinueTeleport(player);
        InteractionController.ForcePlayerLeftBasement();
        OpenExitDoor();
        _skipped = true;

        // One frame so CharacterController / mouse look settle, then fire the real leave sequence.
        yield return null;

        ButcherWindowShot shot = FindFirstObjectByType<ButcherWindowShot>(FindObjectsInactive.Include);
        if (shot != null)
        {
            _leaveShotStarted = shot.TryDebugTrigger();
            if (_leaveShotStarted)
                Debug.Log("[DebugBasementSkip] Teleported outside the basement; leave-basement shot starting.");
            else
                Debug.Log("[DebugBasementSkip] Teleported outside the basement (leave shot already played).");
        }
        else
        {
            Debug.LogWarning("[DebugBasementSkip] Teleported outside, but no ButcherWindowShot to trigger.");
        }
    }

    IEnumerator SkipToBarPickup()
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

        CorpseVanishEvent corpse = FindFirstObjectByType<CorpseVanishEvent>(FindObjectsInactive.Include);
        if (corpse == null)
        {
            Debug.LogWarning("[DebugBasementSkip] No CorpseVanishEvent in the scene.");
            yield break;
        }

        SkipIntroCinematic();
        ReleaseBlockingLocks();

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            player.layer = playerLayer;

        var hider = player.GetComponent<PlayerHider>();
        if (hider != null)
        {
            if (hider.isHidden && hider.CurrentHidingSpot != null)
                hider.CurrentHidingSpot.RevealPlayer(player);
            hider.ClearSearchMemory();
            hider.EnableControls();
            hider.RestoreCollision();
        }

        corpse.DebugPrepareForBarPickup();

        if (!corpse.TryGetBarApproachPose(out Vector3 feet, out float yaw))
        {
            Debug.LogWarning("[DebugBasementSkip] Could not resolve a stand point for the bar.");
            yield break;
        }

        // Match the post-return-scare beat: corridor already left, return door trip finished, cage open.
        FindFirstObjectByType<CorridorTension>(FindObjectsInactive.Include)?.RestoreLeft();
        FindFirstObjectByType<BasementReturnDoorTrap>(FindObjectsInactive.Include)?.DebugSkipReturnTripComplete();

        foreach (PadlockLockpickMinigame padlock in FindObjectsByType<PadlockLockpickMinigame>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            padlock.RestoreUnlocked();

        // Open cage / cage-room access only — leave the corridor escape door alone so
        // BasementBarEscapeTrap can re-stick it after the bar is taken.
        foreach (Cage cage in FindObjectsByType<Cage>(FindObjectsSortMode.None))
        {
            OpenDoor(cage.cageDoor);
            cage.SyncDoorState();
            if (cage.cageDoor != null)
            {
                foreach (ScrewdriverLockBreakMinigame screwdriver in cage.cageDoor.GetComponentsInChildren<ScrewdriverLockBreakMinigame>(true))
                    screwdriver.ResetProgress();
            }
        }

        yield return null;

        Teleport(player, feet, yaw);
        Cage.RebindAllAfterContinueTeleport(player);

        InteractionController interaction = FindFirstObjectByType<InteractionController>();
        InteractionController.ArmBasementGraceUntilCageExit(interaction != null ? interaction.BasementRoutineDelaySeconds : 30f);
        CinematicPlayerControlLock.KickNpcNaturalRoutines();

        Debug.Log("[DebugBasementSkip] Bar pickup skip — he's at the wall, the bar is still there. Take it to test the escape.");
    }

    IEnumerator ListenFromInsideHouse()
    {
        // The shot may have fired on its own the moment we landed outside. It unlocks control before its blend
        // and only sets the butcher hunting at the very end, so wait for HasFinished, not just the lock.
        float giveUp = Time.time + LeaveShotTimeout;
        bool shotPlaying = _leaveShotStarted;
        while (Time.time < giveUp)
        {
            if (PlayerControlGate.IsAcquired(LeaveShotLock))
                shotPlaying = true;
            else if (!shotPlaying || ButcherWindowShot.HasFinished)
                break;
            yield return null;
        }

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
            yield break;
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured) || JumpscareManager.CaptureUiActive)
        {
            Debug.LogWarning("[DebugBasementSkip] Captured before reaching the house; listen mode skipped.");
            yield break;
        }

        if (!TryGetInsidePose(out Vector3 feet, out float yaw))
        {
            Debug.LogWarning("[DebugBasementSkip] No living-room anchor to spawn in the farmhouse.");
            yield break;
        }

        Teleport(player, feet, yaw);
        Cage.RebindAllAfterContinueTeleport(player);

        // After the rebind, which forces the Player layer outside a cage.
        player.layer = CaptiveLayer();
        _listenPlayer = player;
        MutePlayerAudio();

        // The leave shot's hunt is relentless (ignores Captive), so call it off explicitly.
        foreach (NPCController npc in FindObjectsByType<NPCController>(FindObjectsSortMode.None))
        {
            if (npc.fsm == null)
                continue;
            if (npc.currentState == NPCController.AIState.Chase || npc.currentState == NPCController.AIState.Search
                || npc.currentState == NPCController.AIState.Suspicious)
                npc.fsm.CallOffHunt();
        }

        Debug.Log("[DebugBasementSkip] Listen mode: in the living room on the Captive layer, player audio muted. Ctrl+1 to become visible again.");
    }

    static int CaptiveLayer()
    {
        int captiveLayer = LayerMask.NameToLayer("Captive");
        return captiveLayer >= 0 ? captiveLayer : 12;
    }

    // Muted rather than disabled: PlayerFootsteps still calls PlayOneShot, which warns on a disabled source.
    void MutePlayerAudio()
    {
        _listenPlayer.GetComponentsInChildren(true, _sourceScratch);
        foreach (AudioSource source in _sourceScratch)
        {
            if (source.mute)
                continue;
            source.mute = true;
            _mutedSources.Add(source);
        }
    }

    void StopListening()
    {
        foreach (AudioSource source in _mutedSources)
        {
            if (source != null)
                source.mute = false;
        }
        _mutedSources.Clear();
        _listenPlayer = null;
    }

    void ExitListenModeVisible()
    {
        GameObject player = _listenPlayer;
        StopListening();
        if (player == null)
            return;
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            player.layer = playerLayer;
        Debug.Log("[DebugBasementSkip] Listen mode ended — back on the Player layer, audio unmuted.");
    }

    static void Teleport(GameObject player, Vector3 feet, float yaw)
    {
        var body = player.GetComponent<CharacterController>();
        if (body != null)
            body.enabled = false;
        player.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
        if (body != null)
            body.enabled = true;
        Physics.SyncTransforms();

        FindFirstObjectByType<CinemachineMouseLook>()?.SetLook(Quaternion.Euler(0f, yaw, 0f));
    }

    static bool TryGetInsidePose(out Vector3 feet, out float yaw)
    {
        feet = default;
        yaw = 0f;

        Transform anchor = null;
        foreach (string anchorName in HouseSpawnAnchors)
        {
            GameObject go = GameObject.Find(anchorName);
            if (go != null)
            {
                anchor = go.transform;
                break;
            }
        }
        if (anchor == null)
            return false;

        // Floor straight below the anchor first; a loose NavMesh sample can snap to the storey above.
        Vector3 spot = anchor.position;
        if (Physics.Raycast(spot + Vector3.up * 0.25f, Vector3.down, out RaycastHit floor, 4f, ~0, QueryTriggerInteraction.Ignore))
            spot = floor.point;
        if (NavMesh.SamplePosition(spot, out NavMeshHit navHit, 1.5f, NavMesh.AllAreas)
            && Mathf.Abs(navHit.position.y - spot.y) < 0.6f)
            spot = navHit.position;
        if (Physics.Raycast(spot + Vector3.up * 0.75f, Vector3.down, out RaycastHit hit, 1.5f, ~0, QueryTriggerInteraction.Ignore))
            spot = hit.point;
        feet = spot;

        GameObject house = GameObject.Find(FarmhousePath);
        Vector3 facing = house != null ? Flat(BoundsOf(house.transform).center - feet) : Vector3.zero;
        yaw = facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing, Vector3.up).eulerAngles.y : anchor.eulerAngles.y;
        return true;
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
        OpenDoor(door);
    }

    static void OpenDoor(SimpleOpenClose door)
    {
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
        while (PlayerControlGate.IsAcquired(LeaveShotLock))
            PlayerControlGate.Release(LeaveShotLock);
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
