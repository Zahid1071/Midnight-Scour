using UnityEngine;
using FS_Atmo;

public class Cage : MonoBehaviour
{
    [Header("References")]
    public SimpleOpenClose cageDoor;
    public bool isOccupied = false;

    /// <summary>
    /// Set when the player leaves after actually being held. NPCs use this so an empty/open
    /// cage during normal play does not look like an escape.
    /// </summary>
    public bool PendingEscapeSighting { get; private set; }

    public void ConsumeEscapeSighting() => PendingEscapeSighting = false;

    /// <summary>True if this cage currently holds the given player (root or child).</summary>
    public bool HoldsPlayer(Transform player)
    {
        if (!isOccupied || playerRef == null || player == null) return false;
        Transform root = player.root != null ? player.root : player;
        return playerRef.transform == root || playerRef == player.gameObject;
    }

    /// <summary>True if any cage in the scene currently holds this player.</summary>
    public static bool IsPlayerInsideAnyCage(Transform player)
    {
        if (player == null) return false;
#if UNITY_2023_1_OR_NEWER
        var cages = Object.FindObjectsByType<Cage>(FindObjectsSortMode.None);
#else
        var cages = Object.FindObjectsOfType<Cage>();
#endif
        for (int i = 0; i < cages.Length; i++)
        {
            if (cages[i] != null && cages[i].HoldsPlayer(player))
                return true;
        }
        return false;
    }

    /// <summary>True if this world point sits in any cage's floor footprint (standing height counts).</summary>
    public static bool ContainsWorldPoint(Vector3 worldPoint)
    {
#if UNITY_2023_1_OR_NEWER
        var cages = Object.FindObjectsByType<Cage>(FindObjectsSortMode.None);
#else
        var cages = Object.FindObjectsOfType<Cage>();
#endif
        for (int i = 0; i < cages.Length; i++)
        {
            var cage = cages[i];
            if (cage == null)
                continue;

            Collider triggerCol = cage.GetComponent<Collider>();
            if (triggerCol == null || !triggerCol.isTrigger)
                continue;

            Bounds b = triggerCol.bounds;
            if (worldPoint.x >= b.min.x && worldPoint.x <= b.max.x
                && worldPoint.z >= b.min.z && worldPoint.z <= b.max.z)
                return true;
        }

        return false;
    }

    [Header("NPC Noise")]
    [Tooltip("Footstep-suspicion value added to NPCs whenever the cage door opens or closes.")]
    [Min(0f)]
    public float doorSoundSuspicion = 300f;
    [Tooltip("Distance at which the cage door sound is barely audible to NPCs.")]
    [Min(0f)]
    public float doorSoundMaxHearDistance = 150f;
    [Tooltip("Distance within which the cage door sound is heard at full strength.")]
    [Min(0f)]
    public float doorSoundMinHearDistance = 5f;

    [Header("Escape voice line")]
    [Tooltip("Player line played once, the first time they walk out of the open cage.")]
    [SerializeField] private AudioClip firstExitVoice;
    [SerializeField, Range(0f, 1f)] private float firstExitVoiceVolume = 1f;
    [Min(0f)][SerializeField] private float firstExitVoiceDelay = 0.6f;
    [SerializeField] private string firstExitSubtitleKey = "player.voice.what_place";
    private bool _firstExitVoicePlayed;

    // Hardcoded layer ID as requested
    private const int CAPTIVE_LAYER = 12;
    private static int HiddenPlayerLayer = -1;
    private static int PlayerLayer = -1;

    // Variables to track player state
    private int originalLayerIndex;
    private GameObject playerRef;
    private bool _doorWasOpen;

    /// <summary>
    /// Cage/hiding-spot trigger boundaries are tight, so brief overlap flicker (root-motion during
    /// the capture struggle, physics-step/frame-rate timing, movement speed) can fire a spurious
    /// OnTriggerExit+OnTriggerEnter pair while the player is already on a "special" perception layer
    /// (Captive/HiddenPlayer). If that re-Enter is ever trusted as the "original" layer, the player
    /// gets restored to Captive/HiddenPlayer forever on exit - permanently blind/deaf to every NPC for
    /// the rest of the session, with no scene reload to reset it. Never accept those layers as the
    /// "original" - always fall back to the real Player layer.
    /// </summary>
    private static int SanitizeOriginalLayer(int layer) =>
        (layer == CAPTIVE_LAYER || layer == HiddenPlayerLayer) ? PlayerLayer : layer;

    private void Awake()
    {
        // NameToLayer can't run in a field initializer/constructor - must be resolved here.
        if (PlayerLayer < 0) PlayerLayer = LayerMask.NameToLayer("Player");
        if (HiddenPlayerLayer < 0) HiddenPlayerLayer = LayerMask.NameToLayer("HiddenPlayer");

        if (cageDoor != null)
            _doorWasOpen = cageDoor.objectOpen;
    }

    private void Update()
    {
        // If the player starts inside the trigger (e.g. scene start/load),
        // OnTriggerEnter may never fire, so we re-detect occupancy cheaply.
        EnsureOccupancy();

        if (cageDoor != null)
        {
            bool isOpenNow = cageDoor.objectOpen;
            if (isOpenNow != _doorWasOpen)
            {
                if (isOpenNow)
                    GameplayHintManager.Instance?.NotifyCageDoorOpenedFirstTime();
                else if (isOccupied)
                    PunishWatchedDoorClose();

                NotifyNpcsOfDoorSound();
            }

            _doorWasOpen = isOpenNow;
        }

        // Safety check
        if (!isOccupied || playerRef == null || cageDoor == null) return;

        // Door OPEN   -> original layer (visible)
        // Door CLOSED -> Captive only if no NPC can see the cage. If they can see you
        // slam it, stay visible and PunishWatchedDoorClose starts a relentless hunt.
        int targetLayer = originalLayerIndex;

        if (!cageDoor.objectOpen)
        {
            if (!IsCageVisibleToAnyNPC())
                targetLayer = CAPTIVE_LAYER;
        }

        // Only assign if different to avoid overhead
        if (playerRef.layer != targetLayer)
            playerRef.layer = targetLayer;
    }

    /// <summary>
    /// Slam the cage shut while an NPC can see you → relentless capture (no Captive hide).
    /// </summary>
    void PunishWatchedDoorClose()
    {
        if (playerRef == null)
            return;
        if (PlayerControlGate.IsAcquired(PlayerControlGate.ReasonCinematic))
            return;

#if UNITY_2023_1_OR_NEWER
        var perceivers = Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None);
#else
        var perceivers = Object.FindObjectsOfType<PerceptionController>();
#endif

        for (int i = 0; i < perceivers.Length; i++)
        {
            var perception = perceivers[i];
            if (perception == null)
                continue;

            // Must actually see the player (not just hear the door).
            if (!perception.HasDirectLineOfSightToPlayer())
                continue;

            var npc = perception.GetComponent<NPCController>();
            if (npc == null)
                continue;

            npc.ForceRelentlessHunt(CaptureReason.Spotted);
        }
    }

    void NotifyNpcsOfDoorSound()
    {
        if (doorSoundSuspicion <= 0f)
            return;

        Vector3 soundPosition = cageDoor != null ? cageDoor.transform.position : transform.position;

#if UNITY_2023_1_OR_NEWER
        foreach (var perception in Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None))
#else
        foreach (var perception in Object.FindObjectsOfType<PerceptionController>())
#endif
        {
            if (perception != null)
                perception.HearNoiseAt(
                    soundPosition,
                    doorSoundSuspicion,
                    doorSoundMaxHearDistance,
                    doorSoundMinHearDistance,
                    playInvestigateSfx: false);
        }
    }

    private bool IsCageVisibleToAnyNPC()
    {
#if UNITY_2023_1_OR_NEWER
        var perceivers = Object.FindObjectsByType<PerceptionController>(FindObjectsSortMode.None);
#else
        var perceivers = Object.FindObjectsOfType<PerceptionController>();
#endif

        for (int i = 0; i < perceivers.Length; i++)
        {
            var p = perceivers[i];
            if (p != null && p.HasDirectLineOfSightToPlayer())
                return true;
        }

        return false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
            return;

        GameObject root = other.transform.root != null ? other.transform.root.gameObject : other.gameObject;

        // Guard against a spurious re-Enter while we're already tracking this same player as
        // occupied (trigger-boundary flicker) - don't clobber the already-correct originalLayerIndex.
        if (isOccupied && playerRef == root)
            return;

        // Capture the layer the player had when they walked in (sanitized so a mid-flicker
        // re-Enter can never "learn" Captive/HiddenPlayer as the original layer).
        originalLayerIndex = SanitizeOriginalLayer(other.gameObject.layer);

        // Track the root so we don't accidentally toggle only a child collider.
        playerRef = root;
        isOccupied = true;
        PendingEscapeSighting = false;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            // Only arm the escape cue if they were actually inside — empty cages are the default.
            if (isOccupied && playerRef != null)
            {
                PendingEscapeSighting = true;
                TryPlayFirstExitVoice();
            }

            isOccupied = false;

            // Restore the player to their original layer upon exit
            if (playerRef != null)
            {
                playerRef.layer = SanitizeOriginalLayer(originalLayerIndex);
            }

            playerRef = null;

            // Basement grace: timer starts only after leaving the cage (same gate idea as thunder).
            InteractionController.NotifyPlayerLeftCage();
        }
    }

    /// <summary>
    /// Continue teleports the player without reliable OnTriggerExit/Enter.
    /// Clear stale occupancy, then rebind if they landed inside the cage volume.
    /// Does not fire escape sighting; grace Notify is handled by exit / Arm TryStart.
    /// </summary>
    public void RebindAfterContinueTeleport(GameObject player)
    {
        if (player == null)
            return;

        GameObject root = player.transform.root != null ? player.transform.root.gameObject : player;

        // Drop stale "still inside" from before the teleport.
        isOccupied = false;
        playerRef = null;
        PendingEscapeSighting = false;

        Physics.SyncTransforms();

        Collider triggerCol = GetComponent<Collider>();
        if (triggerCol == null || !triggerCol.isTrigger)
            return;

        if (!IsPlayerInsideTrigger(triggerCol, root))
            return;

        // Landed in the cage (normal Continue spawn) — treat as held, original = Player.
        int playerLayer = PlayerLayer >= 0 ? PlayerLayer : LayerMask.NameToLayer("Player");
        originalLayerIndex = SanitizeOriginalLayer(playerLayer >= 0 ? playerLayer : root.layer);
        playerRef = root;
        isOccupied = true;

        // Door closed → Captive this frame so perception matches a fresh start.
        if (cageDoor != null && !cageDoor.objectOpen && playerLayer >= 0)
            root.layer = CAPTIVE_LAYER;
        else if (playerLayer >= 0)
            root.layer = playerLayer;
    }

    /// <summary>Rebind every cage after a Continue teleport.</summary>
    public static void RebindAllAfterContinueTeleport(GameObject player)
    {
#if UNITY_2023_1_OR_NEWER
        var cages = Object.FindObjectsByType<Cage>(FindObjectsSortMode.None);
#else
        var cages = Object.FindObjectsOfType<Cage>();
#endif
        for (int i = 0; i < cages.Length; i++)
        {
            if (cages[i] != null)
                cages[i].RebindAfterContinueTeleport(player);
        }
    }

    void EnsureOccupancy()
    {
        Collider triggerCol = GetComponent<Collider>();
        if (triggerCol == null)
            return;

        // Must be a trigger volume for this to make sense.
        if (!triggerCol.isTrigger)
            return;

        var player = GameObject.FindWithTag("Player");
        if (player == null)
            return;

        GameObject root = player.transform.root != null ? player.transform.root.gameObject : player;
        bool inside = IsPlayerInsideTrigger(triggerCol, root);

        if (inside)
        {
            if (isOccupied && playerRef == root)
                return;

            playerRef = root;
            originalLayerIndex = SanitizeOriginalLayer(playerRef.layer);
            isOccupied = true;
            return;
        }

        // Player teleported / forced out without OnTriggerExit — clear stale hold.
        if (isOccupied && playerRef == root)
        {
            TryPlayFirstExitVoice();
            isOccupied = false;
            playerRef.layer = SanitizeOriginalLayer(originalLayerIndex);
            playerRef = null;
            // Same as a real exit: start basement grace if it was armed for cage-exit.
            InteractionController.NotifyPlayerLeftCage();
        }
    }

    // Door must be open: trigger-boundary flicker can fake an exit while the player is still locked in.
    void TryPlayFirstExitVoice()
    {
        if (_firstExitVoicePlayed || firstExitVoice == null || cageDoor == null || !cageDoor.objectOpen)
            return;
        _firstExitVoicePlayed = true;
        StartCoroutine(FirstExitVoiceRoutine());
    }

    System.Collections.IEnumerator FirstExitVoiceRoutine()
    {
        if (firstExitVoiceDelay > 0f)
            yield return new WaitForSeconds(firstExitVoiceDelay);

        var player = GameObject.FindWithTag("Player");
        if (player != null && IsPlayerInsideAnyCage(player.transform))
        {
            _firstExitVoicePlayed = false;
            yield break;
        }

        if (SoundManager.Instance == null)
            yield break;
        SoundManager.Instance.PlaySFX(firstExitVoice, firstExitVoiceVolume);
        if (HudManager.Instance != null && !string.IsNullOrEmpty(firstExitSubtitleKey))
            HudManager.Instance.ShowSubtitle(Loc.Get(firstExitSubtitleKey), firstExitVoice.length + 1.5f);
    }

    /// <summary>
    /// Floor-slab triggers are short in Y; the player pivot sits above them so
    /// bounds.Contains(pivot) always fails and clears occupancy every frame.
    /// Prefer capsule/CC overlap; fall back to XZ footprint for standing players.
    /// </summary>
    static bool IsPlayerInsideTrigger(Collider triggerCol, GameObject root)
    {
        if (triggerCol == null || root == null)
            return false;

        Bounds triggerBounds = triggerCol.bounds;

        var cc = root.GetComponent<CharacterController>();
        if (cc != null && cc.enabled && triggerBounds.Intersects(cc.bounds))
            return true;

        var col = root.GetComponent<Collider>();
        if (col != null && col.enabled && !col.isTrigger && triggerBounds.Intersects(col.bounds))
            return true;

        // CapsuleCollider on the player is a trigger — still use its world bounds for overlap.
        var capsule = root.GetComponent<CapsuleCollider>();
        if (capsule != null && capsule.enabled && triggerBounds.Intersects(capsule.bounds))
            return true;

        // Standing on a floor slab: horizontal footprint is enough.
        Vector3 p = root.transform.position;
        return p.x >= triggerBounds.min.x && p.x <= triggerBounds.max.x
            && p.z >= triggerBounds.min.z && p.z <= triggerBounds.max.z;
    }
}