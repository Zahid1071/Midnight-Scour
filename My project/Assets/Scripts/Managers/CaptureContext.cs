using UnityEngine;
using UnityEngine.Playables;
using FS_Atmo;

/// <summary>Why the player was captured — set before jumpscare, shown on the game-over screen.</summary>
public enum CaptureReason
{
    Unknown = 0,
    CaughtInOpen,
    WitnessedHide,
    HideNoise,
    SameHideSpot,
    HammerTimingFail,
    Ambush,
    /// <summary>Spotted in the open from Natural (vision, no noise-led chase).</summary>
    Spotted,
}

/// <summary>
/// Sticky capture cause for the current death. Higher-priority reasons do not get overwritten
/// by weaker ones (e.g. hammer-timing hunt stays even if they hide; mic hide-noise beats ambush).
/// </summary>
public static class CaptureContext
{
    public static CaptureReason Pending { get; private set; } = CaptureReason.Unknown;

    public static void Set(CaptureReason reason)
    {
        if (Priority(reason) >= Priority(Pending))
            Pending = reason;
    }

    public static CaptureReason Consume()
    {
        CaptureReason reason = Pending;
        Pending = CaptureReason.Unknown;
        return reason;
    }

    public static void Clear() => Pending = CaptureReason.Unknown;

    public static void ClearIf(CaptureReason reason)
    {
        if (Pending == reason)
            Pending = CaptureReason.Unknown;
    }

    public static string LocalizationKey(CaptureReason reason) => reason switch
    {
        CaptureReason.WitnessedHide => "gameover.witnessed_hide",
        CaptureReason.HideNoise => "gameover.hide_noise",
        CaptureReason.SameHideSpot => "gameover.same_hide_spot",
        CaptureReason.HammerTimingFail => "gameover.hammer_timing",
        CaptureReason.Ambush => "gameover.ambush",
        CaptureReason.Spotted => "gameover.caught_open",
        // Open chase after noise uses the same copy as hide-noise deaths.
        CaptureReason.CaughtInOpen => "gameover.hide_noise",
        _ => "gameover.unknown",
    };

    public static string FallbackEnglish(CaptureReason reason) => reason switch
    {
        CaptureReason.WitnessedHide => "Butcher saw you hiding.",
        CaptureReason.HideNoise => "You made too much noise.",
        CaptureReason.SameHideSpot => "You kept hiding in the same spot.",
        CaptureReason.HammerTimingFail => "You failed to mask Hammer noise with thunder.",
        CaptureReason.Ambush => "Butcher ambushed you.",
        CaptureReason.Spotted => "Butcher saw you.",
        CaptureReason.CaughtInOpen => "You made too much noise.",
        _ => "You were caught.",
    };

    static int Priority(CaptureReason reason) => reason switch
    {
        // 3rd mistimed hammer hunt — sticky over hide-noise / open chase tags.
        CaptureReason.HammerTimingFail => 55,
        // Vision from Natural — sticky for that chase (beats later "too much noise" tags).
        CaptureReason.Spotted => 52,
        // Mic hide-noise is the one ambush exception — keep "too much noise" over ambush copy.
        CaptureReason.HideNoise => 50,
        CaptureReason.Ambush => 45,
        CaptureReason.WitnessedHide => 30,
        CaptureReason.SameHideSpot => 20,
        CaptureReason.CaughtInOpen => 10,
        _ => 0,
    };
}

/// <summary>
/// Lockpick Continue = in-scene day reset (Granny-style).
/// Keeps inventory and puzzle progress; hard-resets player/butcher/capture state;
/// closes the cage door; replays the Main Scene gameplay intro timeline.
/// </summary>
public static class CaptureContinue
{
    static Vector3 _spawnPos;
    static Quaternion _spawnRot;
    static bool _haveSpawn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => ClearSpawnCache();

    public static void RememberPlayerSpawn(Transform player)
    {
        if (player == null || _haveSpawn)
            return;

        _spawnPos = player.position;
        _spawnRot = player.rotation;
        _haveSpawn = true;
    }

    public static void ClearSpawnCache() => _haveSpawn = false;

    public static void Apply()
    {
        JumpscareManager.Instance?.EndJumpscare();
        // Always clear capture UI/gate leftovers (EndJumpscare clears the flag first).
        if (JumpscareManager.CaptureUiActive)
            JumpscareManager.CancelCaptureUi();
        else if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCaptured))
            PlayerControlGate.Release(PlayerControlGate.ReasonCaptured);

        PlayerControlGate.Clear();
        // Lock intro immediately so butcher cannot patrol/chase before Timeline signals.
        PlayerControlGate.Acquire(PlayerControlGate.ReasonCinematic);
        Time.timeScale = 1f;
        AudioListener.pause = false;
        CaptureContext.Clear();
        HudManager.Instance?.ClearSubtitle(HudManager.PriorityHigh);

        Object.FindFirstObjectByType<PauseManager>(FindObjectsInactive.Include)
            ?.RestoreGameplayScriptsAfterGameOver();

        RestoreNpcs();
        HoldBasementGraceForContinueIntro();
        SpawnFreshContinuePadlock();
        CloseCageDoor();

        var playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo != null)
            RestorePlayer(playerGo);

        // Clear hide-capture latches first, then hide for the Continue timeline intro.
        GameplayHudVisibility.Instance?.ClearAllStickyHides();
        GameplayHudVisibility.Instance?.HideGameplayHud();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        ReplayGameplayTimeline();
    }

    static void HoldBasementGraceForContinueIntro()
    {
        var interaction = Object.FindFirstObjectByType<InteractionController>();
        float delay = interaction != null ? interaction.BasementRoutineDelaySeconds : 30f;
        // Do NOT start the 30s clock here — hold through intro, then arm the normal
        // leave-cage gate on UnlockCinematic (same as a fresh start).
        InteractionController.HoldBasementGraceUntilManualStart(delay);
    }

    static void SpawnFreshContinuePadlock()
    {
        var spawner = ContinuePadlockSpawner.Instance
            ?? Object.FindFirstObjectByType<ContinuePadlockSpawner>(FindObjectsInactive.Include);

        if (spawner == null)
        {
            Debug.LogWarning("[CaptureContinue] No ContinuePadlockSpawner in scene.");
            return;
        }

        spawner.SpawnFresh();
    }

    static void ReplayGameplayTimeline()
    {
        var directors = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        PlayableDirector best = null;
        for (int i = 0; i < directors.Length; i++)
        {
            var d = directors[i];
            if (d == null || d.playableAsset == null)
                continue;

            if (d.playableAsset.name.IndexOf("GameplayTimeline", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                best = d;
                break;
            }

            if (best == null && d.gameObject.CompareTag("MainCamera"))
                best = d;
        }

        if (best == null && Camera.main != null)
            best = Camera.main.GetComponent<PlayableDirector>();

        if (best == null)
        {
            Debug.LogWarning("[CaptureContinue] No GameplayTimeline found - unlocking HUD.");
            GameplayHudVisibility.Instance?.ShowGameplayHud();
            // No intro to wait for — arm the normal leave-cage gate now.
            InteractionController.ArmBasementGraceAfterContinueIntro();
            return;
        }

        // Keep scene default as Play On Awake so Restart / New Game also get the intro.
        best.initialTime = 0.0;
        best.time = 0.0;
        best.Stop();
        best.Evaluate();
        best.Play();

        // Same-frame Stop→Play can no-op after a jumpscare; force again next frame.
        var host = best.GetComponent<MonoBehaviour>()
            ?? Object.FindFirstObjectByType<JumpscareManager>(FindObjectsInactive.Include);
        if (host != null)
            host.StartCoroutine(PlayDirectorNextFrame(best));
    }

    static System.Collections.IEnumerator PlayDirectorNextFrame(PlayableDirector director)
    {
        yield return null;
        if (director == null)
            yield break;

        if (director.state != PlayState.Playing)
        {
            director.time = 0.0;
            director.Play();
        }
    }

    static void CloseCageDoor()
    {
        var cage = Object.FindFirstObjectByType<Cage>();
        if (cage == null || cage.cageDoor == null)
        {
            Debug.LogWarning("[CaptureContinue] Cage / cageDoor missing — chained lock not applied.");
            return;
        }

        var doorController = cage.cageDoor.GetComponentInParent<DoorController>();
        if (doorController != null)
        {
            doorController.ForceCloseInstant();
            doorController.SetLocked(true);
        }
        else
        {
            cage.cageDoor.ForceCloseInstant();
        }

        // Continue run: door is chained — no screwdriver break; pick the padlock instead.
        cage.cageDoor.ApplyContinueChainedLock("gameplay.door_chained");
    }

    static void RestorePlayer(GameObject playerGo)
    {
        var hider = playerGo.GetComponent<PlayerHider>();
        if (hider != null)
        {
            if (hider.isHidden && hider.CurrentHidingSpot != null)
                hider.CurrentHidingSpot.RevealPlayer(playerGo);
            hider.ClearSearchMemory();
            hider.EnableControls();
            hider.RestoreCollision();
        }

        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            playerGo.layer = playerLayer;

        var rhythm = playerGo.GetComponent<PlayerFootstepRhythm>();
        if (rhythm != null)
            rhythm.enabled = true;

        if (_haveSpawn)
            Teleport(playerGo.transform, _spawnPos, _spawnRot);

        // Teleport skips triggers — rebind cage occupancy + Captive layer like a fresh start.
        Cage.RebindAllAfterContinueTeleport(playerGo);

        HidingSpot.EndHidingCameraForCapture();

        var dimmer = playerGo.GetComponent<PlayerSpotlightWallDimmer>()
            ?? playerGo.GetComponentInChildren<PlayerSpotlightWallDimmer>(true);
        dimmer?.ResetAfterCaptureContinue();
    }

    static void Teleport(Transform player, Vector3 pos, Quaternion rot)
    {
        var cc = player.GetComponent<CharacterController>();
        if (cc != null)
            cc.enabled = false;

        player.SetPositionAndRotation(pos, rot);

        if (cc != null)
            cc.enabled = true;
    }

    static void RestoreNpcs()
    {
        var npcs = Object.FindObjectsByType<NPCController>(FindObjectsSortMode.None);
        for (int i = 0; i < npcs.Length; i++)
        {
            if (npcs[i] != null)
                npcs[i].ResetAfterCaptureContinue();
        }
    }
}
